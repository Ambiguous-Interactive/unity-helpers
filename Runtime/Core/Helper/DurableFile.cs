// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WallstopStudios.UnityHelpers.Core.Threading;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Stages complete writes for player-owned data such as saves, settings and ledgers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="File.WriteAllText(string, string)"/> truncates the destination before writing a
    /// single byte, so an interrupted write replaces a valid document with a partial one. Every
    /// write here stages the new contents in a sibling file, forces them out of the page cache, and
    /// only then swaps the staged file over the destination.
    /// </para>
    /// <para>
    /// Each replacement publishes complete contents. Directory updates can still be lost on power
    /// failure. Unsupported replacement reports failure and preserves the existing destination.
    /// On Windows, a replacement uses a temporary backup of the previous file. If Windows
    /// moves the previous file aside and then refuses publication, this helper tries to restore it.
    /// If restoration fails, the returned error identifies the backup path.
    /// Windows sharing violations and inability to remove the replaced file receive up to three
    /// replacement attempts with 10 ms and 20 ms waits while retaining staged contents and ownership.
    /// Creating an absent destination remains supported. Cooperating writes, appends, and deletes
    /// serialize per destination in this process and refuse concurrent ownership across processes.
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
        internal const string OwnershipSuffix = ".lock";

        private const int MaximumInitializationStageNameLength = 32;
        private const int MaximumInitializationStageAttempts = 64;
        private const int UnixOperationNotPermitted = 1;
        private const int UnixNameAlreadyExists = 17;
        private const int UnixInvalidArgument = 22;
        private const int LinuxCurrentDirectory = -100;
        private const int LinuxOperationUnavailable = 38;
        private const int LinuxOperationUnsupported = 95;
        private const int MacOperationUnsupported = 45;
        private const int MacOperationUnavailable = 78;
        private const int MacOperationNotSupportedOnSocket = 102;
        private const uint LinuxRenameNoReplace = 1;
        private const uint MacRenameExclusive = 4;
        private const int DefaultBufferSize = 4096;

        // FileMode.Append does not provide atomic append; bounded per-path gates prevent writer overlap.
        private const int GateCount = 32;
        private const int SharingViolationHResult = unchecked((int)0x80070020);
        private const int UnableToRemoveReplacedHResult = unchecked((int)0x80070497);
        private const int UnableToMoveReplacementAfterBackupHResult = unchecked((int)0x80070499);
        private const int ReplacementAttemptLimit = 3;
        private const int ReplacementRetryDelayMilliseconds = 10;

        private static readonly SemaphoreSlim[] Gates = CreateGates();

        private static readonly char[] InvalidSidecarCharacters = Path.GetInvalidFileNameChars();

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

            string temporaryPath = path + TemporarySuffix;
            using (EnterGate(path))
            {
                try
                {
                    byte[] bytes = Utf8NoByteOrderMark.GetBytes(contents ?? string.Empty);
                    return TryWriteStagedBytes(
                        path,
                        temporaryPath,
                        bytes,
                        0,
                        bytes.Length,
                        out error
                    );
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
            int count = contents?.Length ?? 0;
            return TryWriteAllBytes(path, contents, 0, count, out error);
        }

        /// <summary>Replaces a file with a range from an existing byte array.</summary>
        /// <remarks>Staging and publication use the same guarantees as a full-array write. Keep the range unchanged until this call returns.</remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">The source array. Null is treated as empty.</param>
        /// <param name="offset">The first byte to write.</param>
        /// <param name="count">The number of bytes to write.</param>
        /// <param name="error">The failure when this returns false; null otherwise.</param>
        /// <returns>True when the destination holds the selected range.</returns>
        public static bool TryWriteAllBytes(
            string path,
            byte[] contents,
            int offset,
            int count,
            out Exception error
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                return false;
            }
            byte[] bytes = contents ?? Array.Empty<byte>();
            if (offset < 0 || bytes.Length < offset)
            {
                error = new ArgumentOutOfRangeException(nameof(offset));
                return false;
            }
            if (count < 0 || bytes.Length - offset < count)
            {
                error = new ArgumentOutOfRangeException(nameof(count));
                return false;
            }

            string temporaryPath = path + TemporarySuffix;
            using (EnterGate(path))
            {
                return TryWriteStagedBytes(path, temporaryPath, bytes, offset, count, out error);
            }
        }

        /// <summary>Reads a file into a disposable pooled byte buffer.</summary>
        /// <remarks>
        /// Reads one open file handle. DurableFile writers on the same path wait for it to close.
        /// The initial file length must fit in a byte array. A detected length change reports
        /// failure. External in-place writers can still change
        /// bytes while the stream is open on platforms that permit them. Read only the logical
        /// <see cref="PooledArray{T}.length"/> bytes and dispose the lease after use.
        /// </remarks>
        /// <param name="path">The path of a file with a known length.</param>
        /// <param name="contents">The owned buffer on success; default on failure.</param>
        /// <param name="error">The failure when this returns false; null otherwise.</param>
        /// <returns>True when the complete file was read.</returns>
        public static bool TryReadAllBytes(
            string path,
            out PooledArray<byte> contents,
            out Exception error
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                contents = default;
                error = new ArgumentException("A source path is required.", nameof(path));
                return false;
            }

            PooledArray<byte> rental = default;
            try
            {
                using (EnterGate(path))
                {
                    using FileStream stream = OpenSourceStream(path, useAsync: false);
                    long fileLength = stream.Length;
                    if (int.MaxValue < fileLength)
                    {
                        contents = default;
                        error = new IOException("The file is too large for a byte array.");
                        return false;
                    }

                    int length = (int)fileLength;
                    rental = SystemArrayPool<byte>.Get(length, out byte[] buffer);
                    int offset = 0;
                    while (offset < length)
                    {
                        int read = stream.Read(buffer, offset, length - offset);
                        if (read == 0)
                        {
                            contents = default;
                            error = new EndOfStreamException(
                                "The file ended before its original length."
                            );
                            return false;
                        }
                        offset += read;
                    }
                    int nextByte = stream.ReadByte();
                    if (0 <= nextByte)
                    {
                        contents = default;
                        error = new IOException("The file grew while it was being read.");
                        return false;
                    }
                }

                PooledArray<byte> result = rental;
                rental = default;
                contents = result;
                error = null;
                return true;
            }
            catch (Exception failure)
            {
                contents = default;
                error = failure;
                return false;
            }
            finally
            {
                rental.Dispose();
            }
        }

        /// <summary>
        /// Asynchronously replaces a file's entire contents, staging and flushing before the swap.
        /// </summary>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Text to write. Null is treated as empty.</param>
        /// <param name="cancellationToken">Cancellation is checked immediately before publication; cancellation after that check may still publish.</param>
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
        /// Carries the same durability guarantees as <see cref="TryWriteAllBytes(string, byte[], out Exception)"/>.
        /// Keep the array unchanged until this call completes.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Bytes to write. Null is treated as empty.</param>
        /// <param name="cancellationToken">Cancellation is checked immediately before publication; cancellation after that check may still publish.</param>
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
        /// another process while one is in flight fails rather than corrupting the file. Appends also
        /// share the staging ownership used by this type's writes, creates, and deletes. Empty or null
        /// <paramref name="contents"/> is a no-op success and does not create the file.
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
                FileStream ownership = null;
                try
                {
                    EnsureDirectory(path);
                    if (!TryOpenStagingOwnership(path + TemporarySuffix, out ownership, out error))
                    {
                        return false;
                    }
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
                    ReleaseStagingOwnership(ownership);
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
                FileStream ownership = null;
                try
                {
                    EnsureDirectory(path);
                    if (
                        !TryOpenStagingOwnership(
                            path + TemporarySuffix,
                            out ownership,
                            out Exception ownershipError
                        )
                    )
                    {
                        return ownershipError;
                    }
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
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        /// <summary>Moves a file and its existing sidecars without overwriting any destination.</summary>
        /// <param name="sourcePath">The file to move. Close any database that owns this file first.</param>
        /// <param name="destinationPath">The new path in an existing directory.</param>
        /// <param name="sidecarSuffixes">Unique filename suffixes appended to both paths in order.</param>
        /// <param name="error">The move failure, or an aggregate with that failure followed by restoration failures.</param>
        /// <remarks>
        /// Failed moves restore completed moves in reverse order. Cooperating calls in this process
        /// serialize on every group path. Use directory paths with any symbolic links or other
        /// reparse points resolved. Other processes need caller coordination, and a process
        /// interruption can leave a partly moved group.
        /// </remarks>
        /// <returns>True when the file and all present sidecars moved, or false on failure.</returns>
        public static bool TryMoveWithSidecars(
            string sourcePath,
            string destinationPath,
            ReadOnlySpan<string> sidecarSuffixes,
            out Exception error
        )
        {
            Exception validation = ValidateCopyPaths(sourcePath, destinationPath);
            if (validation != null)
            {
                error = validation;
                return false;
            }

            try
            {
                int count = checked(sidecarSuffixes.Length + 1);
                using PooledArray<(string Source, string Destination, bool Moved)> entries =
                    SystemArrayPool<(string Source, string Destination, bool Moved)>.Get(
                        count,
                        out (string Source, string Destination, bool Moved)[] moves
                    );
                Span<(string Source, string Destination, bool Moved)> plan = moves.AsSpan(0, count);
                uint gates = PlanSidecarMove(sourcePath, destinationPath, sidecarSuffixes, plan);
                Exception failure = MoveSidecarGroup(plan, gates);
                if (failure != null)
                {
                    error = failure;
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception failure)
            {
                error = failure;
                return false;
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
                    if (!TryOpenStagingOwnership(temporaryPath, out ownership, out error))
                    {
                        ReleaseFileStream(source);
                        return false;
                    }
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

                        Swap(temporaryPath, destinationPath);
                    }

                    error = null;
                    return true;
                }
                catch (Exception e)
                {
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
        /// <param name="cancellationToken">Cancellation is checked immediately before publication; cancellation after that check may still publish.</param>
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
        /// another process can create the path after deletion completes. A competing operation using
        /// this type reports failure while another operation owns the destination.
        /// </summary>
        /// <param name="path">File to delete.</param>
        /// <returns>True if the file was deleted or its parent directory is absent.</returns>
        public static bool TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            using (EnterGate(path))
            {
                FileStream ownership = null;
                try
                {
                    string parent = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (string.IsNullOrWhiteSpace(parent))
                    {
                        return false;
                    }

                    if (
                        !TryOpenStagingOwnership(
                            path + TemporarySuffix,
                            out ownership,
                            out Exception ownershipError
                        )
                    )
                    {
                        return ownershipError is DirectoryNotFoundException;
                    }
                    File.Delete(path);
                    return true;
                }
                catch (DirectoryNotFoundException)
                {
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
                finally
                {
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        /// <summary>Initializes absent data without leaving a partial destination after failure.</summary>
        internal static bool TryInitializeAllBytes(string path, byte[] contents)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            using (EnterGate(path))
            {
                string temporaryPath = null;
                bool ownsTemporary = false;
                try
                {
                    string destinationPath = Path.GetFullPath(path);
                    string directory = Path.GetDirectoryName(destinationPath);
                    string filename = Path.GetFileName(destinationPath);
                    if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filename))
                    {
                        return false;
                    }
                    Directory.CreateDirectory(directory);
                    if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                    {
                        return false;
                    }
                    int temporaryNameLength = MaximumInitializationStageNameLength;
                    FileStream staging = null;
                    for (int attempt = 0; attempt < MaximumInitializationStageAttempts; ++attempt)
                    {
                        Guid temporaryIdentifier = Guid.NewGuid();
                        string temporaryName =
                            temporaryNameLength == MaximumInitializationStageNameLength
                                ? temporaryIdentifier.ToString("N")
                                : Convert
                                    .ToBase64String(temporaryIdentifier.ToByteArray())
                                    .TrimEnd('=')
                                    .Replace('+', '-')
                                    .Replace('/', '_');
                        if (temporaryNameLength < temporaryName.Length)
                        {
                            temporaryName = temporaryName.Substring(0, temporaryNameLength);
                        }
                        if (
                            string.Equals(
                                temporaryName,
                                filename,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            continue;
                        }
                        temporaryPath = Path.Combine(directory, temporaryName);
                        try
                        {
                            staging = new FileStream(
                                temporaryPath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None,
                                DefaultBufferSize,
                                useAsync: false
                            );
                            ownsTemporary = true;
                            break;
                        }
                        catch (PathTooLongException) when (filename.Length < temporaryNameLength)
                        {
                            temporaryNameLength = filename.Length;
                        }
                        catch (IOException)
                            when (File.Exists(temporaryPath) || Directory.Exists(temporaryPath)) { }
                    }
                    if (staging == null)
                    {
                        return false;
                    }
                    using (staging)
                    {
                        contents ??= Array.Empty<byte>();
                        staging.Write(contents, 0, contents.Length);
                        staging.Flush(flushToDisk: true);
                    }
                    if (
                        !TryPublishStagedFileWithoutOverwrite(
                            temporaryPath,
                            destinationPath,
                            out bool leavesStaged
                        )
                    )
                    {
                        return false;
                    }
                    ownsTemporary = leavesStaged;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
                finally
                {
                    if (ownsTemporary)
                    {
                        DiscardStagedFile(temporaryPath);
                    }
                }
            }
        }

        /// <summary>Creates a file from flushed bytes only while its destination is absent.</summary>
        /// <remarks>
        /// Unix publication requires a filesystem that supports hard links. On Unix the staged
        /// name is removed after publication and verification; if that cleanup fails, the
        /// destination remains in place and this method reports a failure for inspection.
        /// A noncooperating writer can replace the staged name before cleanup, so that cleanup
        /// does not provide exclusive ownership against external writers. Public DurableFile write,
        /// append, and delete operations cannot mutate the destination while this create owns it.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Bytes to write.</param>
        /// <param name="error">
        /// The failure when this returns false; null otherwise. If staged bytes could not be
        /// published, <see cref="PreservedStagingPathDataKey"/> identifies their path in Data.
        /// </param>
        /// <returns>True when the new file was published without replacing another file.</returns>
        internal static bool TryCreateAllBytes(string path, byte[] contents, out Exception error)
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
                string temporaryPath = null;
                FileStream ownership = null;
                bool ownsTemporary = false;
                bool moved = false;
                try
                {
                    EnsureDirectory(path);
                    if (!TryOpenStagingOwnership(path + TemporarySuffix, out ownership, out error))
                    {
                        return false;
                    }
                    temporaryPath = UniqueCreationStagingPath(path);
                    using (
                        FileStream staging = OpenStagingStream(
                            temporaryPath,
                            useAsync: false,
                            createNew: true
                        )
                    )
                    {
                        ownsTemporary = true;
                        staging.Write(contents, 0, contents.Length);
                        staging.Flush(flushToDisk: true);
                    }

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
                    VerifyCreatedContents(path, contents);
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
                finally
                {
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        /// <summary>
        /// Publishes a staged file only if the destination has no directory entry.
        /// </summary>
        /// <remarks>
        /// A successful hard link leaves the staged name behind for the caller to remove.
        /// A successful rename consumes the staged name. A collision keeps it on every platform.
        /// </remarks>
        /// <returns>True if published, false if the destination was already occupied.</returns>
        internal static bool TryPublishStagedFileWithoutOverwrite(
            string stagedPath,
            string destinationPath,
            out bool leavesStaged,
            bool preferExclusiveRename = false
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

            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            if (!isMac && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException(
                    "Exclusive file publication is unavailable on this platform."
                );
            }
            if (
                preferExclusiveRename
                && TryRenameUnixWithoutOverwrite(
                    stagedPath,
                    destinationPath,
                    isMac,
                    out bool renamed
                )
            )
            {
                leavesStaged = !renamed;
                return renamed;
            }

            int result = isMac
                ? CreateHardLinkMac(stagedPath, destinationPath)
                : CreateHardLinkLinux(stagedPath, destinationPath);
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

        /// <summary>Publishes an editor stage while owning the destination.</summary>
        internal static bool TryPublishOwnedStagedFileWithoutOverwrite(
            string stagedPath,
            string destinationPath,
            out bool leavesStaged
        )
        {
            using (EnterGate(destinationPath))
            {
                if (
                    !TryOpenStagingOwnership(
                        destinationPath + TemporarySuffix,
                        out FileStream ownership,
                        out Exception error
                    )
                )
                {
                    throw error;
                }

                try
                {
                    return TryPublishStagedFileWithoutOverwrite(
                        stagedPath,
                        destinationPath,
                        out leavesStaged
                    );
                }
                finally
                {
                    ReleaseStagingOwnership(ownership);
                }
            }
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
                    if (!TryOpenStagingOwnership(temporaryPath, out ownership, out error))
                    {
                        return false;
                    }
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
        /// DurableFile writers in this process share the destination gate from comparison through
        /// publication. An external writer can change an existing file between those steps.
        /// When the comparison expects absence, publication refuses to overwrite any intervening creation.
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
            string stagedPath = temporaryPath;
            using (EnterGate(path))
            {
                FileStream ownership = null;
                FileStream staging = null;
                bool ownsStaging = false;
                try
                {
                    EnsureDirectory(path);
                    if (!TryOpenStagingOwnership(temporaryPath, out ownership, out error))
                    {
                        exchanged = false;
                        return false;
                    }
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

                    if (!expectedExists)
                    {
                        // A surviving hard-link stage must never be reopened for truncation.
                        stagedPath = UniqueCreationStagingPath(path);
                    }
                    staging = OpenStagingStream(
                        stagedPath,
                        useAsync: false,
                        createNew: !expectedExists
                    );
                    ownsStaging = true;
                    byte[] bytes = Utf8NoByteOrderMark.GetBytes(
                        replacementContents ?? string.Empty
                    );
                    staging.Write(bytes, 0, bytes.Length);
                    staging.Flush(flushToDisk: true);
                    staging.Dispose();
                    staging = null;
                    bool published;
                    if (expectedExists)
                    {
                        Swap(stagedPath, path);
                        ownsStaging = false;
                        published = true;
                    }
                    else
                    {
                        published = TryPublishStagedFileWithoutOverwrite(
                            stagedPath,
                            path,
                            out bool leavesStaged,
                            preferExclusiveRename: true
                        );
                        ownsStaging = leavesStaged;
                    }
                    error = null;
                    exchanged = published;
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
                        DiscardStagedFile(stagedPath);
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
                    if (
                        !TryOpenStagingOwnership(
                            temporaryPath,
                            out ownership,
                            out Exception ownershipError
                        )
                    )
                    {
                        ReleaseFileStream(source);
                        return ownershipError;
                    }
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

                        PublishStagedFile(temporaryPath, destinationPath, cancellationToken);
                    }

                    return null;
                }
                catch (Exception e)
                {
                    DiscardStagedFile(temporaryPath);
                    return e;
                }
                finally
                {
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        /// <summary>Verifies a newly published document before its staged name is removed.</summary>
        internal static void VerifyCreatedContents(string path, byte[] contents)
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(contents))
            {
                throw new IOException(
                    $"Created file at {path}, but its contents differ; inspect it before retrying."
                );
            }
        }

        // Only a successful exclusive staging open establishes cleanup ownership; failed opens may name another writer.
        /// <summary>Opens the staging file exclusively for writing.</summary>
        internal static FileStream OpenStagingStream(
            string temporaryPath,
            bool useAsync,
            bool createNew = false
        )
        {
            return new FileStream(
                temporaryPath,
                createNew ? FileMode.CreateNew : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                DefaultBufferSize,
                useAsync
            );
        }

        // Isolate expected sharing failures from IL2CPP callers and async state machines.
        /// <summary>Acquires cooperating-writer ownership until the returned stream is disposed.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool TryOpenStagingOwnership(
            string temporaryPath,
            out FileStream ownership,
            out Exception error
        )
        {
            try
            {
                ownership = new FileStream(
                    temporaryPath + OwnershipSuffix,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose
                );
                error = null;
                return true;
            }
            catch (IOException e)
            {
                ownership = null;
                error = e;
                return false;
            }
            catch (Exception e)
            {
                ownership = null;
                error = e;
                return false;
            }
        }

        /// <summary>Removes an owned staging file without masking the original failure.</summary>
        internal static void DiscardStagedFile(string temporaryPath)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception) { }
        }

        /// <summary>Publishes completed staging contents unless cancellation was requested.</summary>
        internal static void PublishStagedFile(
            string temporaryPath,
            string path,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Swap(temporaryPath, path);
        }

        // The destination can change after the existence probe; retry the matching alternative on that race.
        /// <summary>Publishes a staging file over an absent or existing destination.</summary>
        internal static void Swap(string temporaryPath, string path)
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

        private static string UniqueCreationStagingPath(string path)
        {
            return Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(path)),
                ".create." + Guid.NewGuid().ToString("N") + TemporarySuffix
            );
        }

        // A hard link is also exclusive and covers volumes that reject the rename flag.
        private static bool TryRenameUnixWithoutOverwrite(
            string stagedPath,
            string destinationPath,
            bool isMac,
            out bool renamed
        )
        {
            int result;
            try
            {
                result = isMac
                    ? RenameExclusiveMac(stagedPath, destinationPath, MacRenameExclusive)
                    : RenameExclusiveLinux(
                        LinuxCurrentDirectory,
                        stagedPath,
                        LinuxCurrentDirectory,
                        destinationPath,
                        LinuxRenameNoReplace
                    );
            }
            catch (EntryPointNotFoundException)
            {
                renamed = false;
                return false;
            }
            if (result == 0)
            {
                renamed = true;
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (error == UnixNameAlreadyExists)
            {
                renamed = false;
                return true;
            }
            bool unavailable = isMac
                ? error
                    is MacOperationUnsupported
                        or MacOperationUnavailable
                        or MacOperationNotSupportedOnSocket
                : error is LinuxOperationUnavailable or LinuxOperationUnsupported;
            if (unavailable || error is UnixOperationNotPermitted or UnixInvalidArgument)
            {
                renamed = false;
                return false;
            }
            throw new IOException(
                $"Could not publish '{destinationPath}' with an exclusive rename (OS error {error})."
            );
        }

        [DllImport("libc", EntryPoint = "link", ExactSpelling = true, SetLastError = true)]
        private static extern int CreateHardLinkLinux(string stagedPath, string destinationPath);

        [DllImport("libc", EntryPoint = "renameat2", ExactSpelling = true, SetLastError = true)]
        private static extern int RenameExclusiveLinux(
            int sourceDirectory,
            string stagedPath,
            int destinationDirectory,
            string destinationPath,
            uint flags
        );

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "link",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int CreateHardLinkMac(string stagedPath, string destinationPath);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "renamex_np",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int RenameExclusiveMac(
            string stagedPath,
            string destinationPath,
            uint flags
        );

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
                    bytes = byteContents
                        ?? (
                            textContents == null
                                ? Array.Empty<byte>()
                                : Utf8NoByteOrderMark.GetBytes(textContents)
                        );
                    EnsureDirectory(path);
                    if (
                        !TryOpenStagingOwnership(
                            temporaryPath,
                            out ownership,
                            out Exception ownershipError
                        )
                    )
                    {
                        return ownershipError;
                    }
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

                    PublishStagedFile(temporaryPath, path, cancellationToken);

                    return null;
                }
                catch (Exception e)
                {
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
            int offset,
            int count,
            out Exception error
        )
        {
            FileStream ownership = null;
            FileStream staging;
            try
            {
                EnsureDirectory(path);
                if (!TryOpenStagingOwnership(temporaryPath, out ownership, out error))
                {
                    return false;
                }
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
                    staging.Write(contents, offset, count);
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
            finally
            {
                ReleaseStagingOwnership(ownership);
            }
        }

        private static uint PlanSidecarMove(
            string sourcePath,
            string destinationPath,
            ReadOnlySpan<string> sidecarSuffixes,
            Span<(string Source, string Destination, bool Moved)> moves
        )
        {
            string source = NormalizeMovePath(sourcePath, nameof(sourcePath));
            string destination = NormalizeMovePath(destinationPath, nameof(destinationPath));
            string sourceParent = Path.GetDirectoryName(source);
            string destinationParent = Path.GetDirectoryName(destination);
            RequireUnaliasedMoveDirectory(sourceParent);
            if (!string.Equals(sourceParent, destinationParent, StringComparison.Ordinal))
            {
                RequireUnaliasedMoveDirectory(destinationParent);
            }

            moves[0] = (source, destination, false);
            int next = 1;
            foreach (string suffix in sidecarSuffixes)
            {
                if (
                    string.IsNullOrWhiteSpace(suffix)
                    || 0 <= suffix.IndexOf('/')
                    || 0 <= suffix.IndexOf('\\')
                    || 0 <= suffix.IndexOfAny(InvalidSidecarCharacters)
                )
                {
                    throw new ArgumentException(
                        "A sidecar suffix must extend the filename.",
                        nameof(sidecarSuffixes)
                    );
                }

                moves[next] = (
                    NormalizeMovePath(source + suffix, nameof(sidecarSuffixes)),
                    NormalizeMovePath(destination + suffix, nameof(sidecarSuffixes)),
                    false
                );
                ++next;
            }

            int pathCount = checked(moves.Length * 2);
            using PooledArray<string> pathLease = SystemArrayPool<string>.Get(
                pathCount,
                out string[] paths
            );
            next = 0;
            uint gateMask = 0;
            foreach ((string from, string to, bool _) in moves)
            {
                paths[next++] = from;
                paths[next++] = to;
                gateMask |= (1u << GateIndexFor(from)) | (1u << GateIndexFor(to));
            }

            Array.Sort(paths, 0, pathCount, StringComparer.OrdinalIgnoreCase);
            for (int index = 1; index < pathCount; ++index)
            {
                if (
                    string.Equals(
                        paths[index - 1],
                        paths[index],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new ArgumentException(
                        "A file or sidecar appears more than once in the move."
                    );
                }
            }

            return gateMask;
        }

        private static string NormalizeMovePath(string path, string parameter)
        {
            string fullPath = Path.GetFullPath(path);
            string filename = Path.GetFileName(fullPath);
            if (
                string.IsNullOrWhiteSpace(filename)
                || (
                    Path.DirectorySeparatorChar == '\\'
                    && (
                        filename.EndsWith(".", StringComparison.Ordinal)
                        || filename.EndsWith(" ", StringComparison.Ordinal)
                    )
                )
            )
            {
                throw new ArgumentException(
                    "The move requires an unambiguous filename.",
                    parameter
                );
            }

            return fullPath;
        }

        private static void RequireUnaliasedMoveDirectory(string parent)
        {
            for (string directory = parent; !string.IsNullOrEmpty(directory); )
            {
                string ancestor = Path.GetDirectoryName(directory);
                if (ancestor == null)
                {
                    break;
                }

                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"Use the resolved directory path in place of '{directory}'."
                    );
                }

                directory = ancestor;
            }
        }

        private static Exception MoveSidecarGroup(
            Span<(string Source, string Destination, bool Moved)> moves,
            uint gateMask
        )
        {
            using PooledArray<SemaphoreLease> gateLease = SystemArrayPool<SemaphoreLease>.Get(
                GateCount,
                out SemaphoreLease[] heldGates
            );
            int heldCount = 0;
            try
            {
                // Acquire each bucket once, in the same order for every move group.
                for (int index = 0; index < GateCount; ++index)
                {
                    if ((gateMask & (1u << index)) != 0)
                    {
                        heldGates[heldCount] = Gates[index].Acquire();
                        ++heldCount;
                    }
                }

                int count = moves.Length;
                for (int index = 0; index < count; ++index)
                {
                    ref (string Source, string Destination, bool Moved) move = ref moves[index];
                    FileAttributes? sourceAttributes = ReadMoveAttributes(move.Source);
                    if (!sourceAttributes.HasValue && 0 < index)
                    {
                        if (ReadMoveAttributes(move.Destination).HasValue)
                        {
                            throw new IOException(
                                $"The destination already has a sidecar at '{move.Destination}'."
                            );
                        }

                        continue;
                    }

                    if (
                        sourceAttributes.HasValue
                        && (sourceAttributes.Value & FileAttributes.Directory) != 0
                    )
                    {
                        throw new IOException(
                            $"Cannot move a directory as a file: '{move.Source}'."
                        );
                    }

                    File.Move(move.Source, move.Destination);
                    move.Moved = true;
                    RequireMoveSourceRemoved(move.Source, move.Destination);
                }

                return null;
            }
            catch (Exception failure)
            {
                return RestoreSidecarMoves(moves, failure);
            }
            finally
            {
                for (int index = heldCount - 1; 0 <= index; --index)
                {
                    heldGates[index].Dispose();
                }
            }
        }

        private static void RequireMoveSourceRemoved(string source, string destination)
        {
            if (ReadMoveAttributes(source).HasValue)
            {
                throw new IOException(
                    $"The move from '{source}' to '{destination}' left the source in place."
                );
            }
        }

        private static FileAttributes? ReadMoveAttributes(string path)
        {
            try
            {
                return File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        private static Exception RestoreSidecarMoves(
            ReadOnlySpan<(string Source, string Destination, bool Moved)> moves,
            Exception failure
        )
        {
            List<Exception> failures = null;
            for (int index = moves.Length - 1; 0 <= index; --index)
            {
                (string source, string destination, bool moved) = moves[index];
                if (!moved)
                {
                    continue;
                }

                try
                {
                    File.Move(destination, source);
                    RequireMoveSourceRemoved(destination, source);
                }
                catch (Exception restoreFailure)
                {
                    failures ??= new List<Exception> { failure };
                    failures.Add(
                        new IOException(
                            $"Could not restore '{source}' from '{destination}'.",
                            restoreFailure
                        )
                    );
                }
            }

            return failures == null
                ? failure
                : new AggregateException(
                    "The move failed and some files could not be restored.",
                    failures
                );
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
            int gatesLength = gates.Length;
            for (int i = 0; i < gatesLength; ++i)
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
            return Gates[GateIndexFor(path)];
        }

        private static int GateIndexFor(string path)
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
            return hash.PositiveMod(GateCount);
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
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void Replace(string temporaryPath, string path)
        {
            string backupPath = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // ReplaceFileW can remove the destination on error 1176 when no backup is requested.
                string directory = Path.GetDirectoryName(path) ?? string.Empty;
                string backupName = $".{Guid.NewGuid():N}.bak";
                backupPath = Path.Combine(directory, backupName);
            }

            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    File.Replace(temporaryPath, path, backupPath);
                    if (backupPath != null)
                    {
                        DiscardStagedFile(backupPath);
                    }
                    return;
                }
                catch (IOException failure)
                    when (attempt < ReplacementAttemptLimit
                        && (
                            failure.HResult == SharingViolationHResult
                            || failure.HResult == UnableToRemoveReplacedHResult
                        )
                    )
                {
                    // These Windows errors retain both names, so the same staged bytes can be retried.
                    Thread.Sleep(ReplacementRetryDelayMilliseconds * attempt);
                }
                catch (IOException failure)
                    when (backupPath != null
                        && failure.HResult == UnableToMoveReplacementAfterBackupHResult
                    )
                {
                    try
                    {
                        File.Move(backupPath, path);
                    }
                    catch (Exception restoreFailure)
                    {
                        AggregateException details = new(failure, restoreFailure);
                        throw new IOException(
                            $"Could not restore the previous file from '{backupPath}'.",
                            details
                        );
                    }
                    throw;
                }
                catch (FileNotFoundException)
                {
                    File.Move(temporaryPath, path);
                    return;
                }
            }
        }
    }
}
