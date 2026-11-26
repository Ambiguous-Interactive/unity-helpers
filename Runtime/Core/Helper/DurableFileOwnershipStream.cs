// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || !UNITY_5_3_OR_NEWER
#define DURABLE_FILE_DESKTOP_OWNERSHIP
#endif

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;

    internal sealed class DurableFileOwnershipStream : FileStream
    {
#if DURABLE_FILE_DESKTOP_OWNERSHIP
        private const int LockExclusive = 2;
        private const int LockNonBlocking = 4;
        private const int FileNotFound = 2;
        private const int NotDirectory = 20;
        private const int LinuxCurrentDirectory = -100;
        private const int LinuxNonblockingCloseOnExec = 0x80800;
        private const int MacNonblockingCloseOnExec = 0x1000004;
        private const int FileTypeMask = 0xf000;
        private const int RegularFile = 0x8000;
        private const uint LinuxStatType = 1;
        private const int LinuxEmptyPath = 0x1000;
        private const int LinuxSymlinkNoFollow = 0x100;
        private const uint LinuxStatInode = 0x100;
        private const int LinuxOperationNotPermitted = 1;
        private const int LinuxOperationUnavailable = 38;
        private const int LinuxX64StatVersion = 1;

        private bool _ownsName;
#endif

        private DurableFileOwnershipStream(string lockPath, FileMode mode)
            : base(
                lockPath,
                mode,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1
            ) { }

        internal static FileStream Acquire(string lockPath)
        {
#if DURABLE_FILE_DESKTOP_OWNERSHIP
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            if (isMac || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return AcquireUnix(lockPath, isMac);
            }
#endif
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose
                );
            }
            catch (IOException) when (File.Exists(lockPath))
            {
                RejectLockReparsePoint(lockPath);
                /*
                    An existing entry can hold an older writer, a stale lock, or unrelated data.
                    A handle that did not create the entry must never delete it on close.
                */
                FileStream existing = new(
                    lockPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 1
                );
                try
                {
                    RequireEmptyLock(existing, lockPath);
                    return existing;
                }
                catch
                {
                    existing.Dispose();
                    throw;
                }
            }
        }

        internal static FileStream OpenRegularSource(string path, int bufferSize, bool useAsync)
        {
#if DURABLE_FILE_DESKTOP_OWNERSHIP
            bool mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            if (mac || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OpenUnixRegularSource(path, bufferSize, mac);
            }
#endif
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                useAsync
            );
        }

        private static void RejectLockReparsePoint(string lockPath)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(lockPath);
            }
            catch (FileNotFoundException)
            {
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"The writer lock path is a symbolic link or reparse point: '{lockPath}'."
                );
            }
        }

        private static void RequireEmptyLock(FileStream stream, string lockPath)
        {
            if (stream.Length != 0)
            {
                throw new IOException($"The writer lock path contains data: '{lockPath}'.");
            }
        }

#if DURABLE_FILE_DESKTOP_OWNERSHIP
        private static FileStream OpenUnixRegularSource(string path, int bufferSize, bool mac)
        {
            int descriptor = mac
                ? OpenSourceMac(path, MacNonblockingCloseOnExec)
                : OpenSourceLinux(path, LinuxNonblockingCloseOnExec);
            if (descriptor < 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == FileNotFound)
                {
                    throw new FileNotFoundException("The source file does not exist.", path);
                }
                if (error == NotDirectory)
                {
                    throw new DirectoryNotFoundException(
                        $"A parent of '{path}' is not a directory."
                    );
                }
                throw new IOException($"Could not open '{path}' (OS error {error}).");
            }

            SafeFileHandle handle = new(new IntPtr(descriptor), ownsHandle: true);
            try
            {
                if (RegularFile != (UnixFileType(descriptor, path, mac) & FileTypeMask))
                {
                    throw new IOException($"The source must be a regular file: '{path}'.");
                }

                /*
                    A wrapped Unix descriptor is synchronous. ReadAsync still uses the
                    stream's asynchronous read path without requiring a Windows overlapped handle.
                */
                return new FileStream(handle, FileAccess.Read, bufferSize, isAsync: false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static int UnixFileType(int descriptor, string path, bool mac)
        {
            if (mac)
            {
                MacFileIdentity identity;
                int result =
                    RuntimeInformation.ProcessArchitecture == Architecture.X64
                        ? StatIntelMacDescriptor(descriptor, out identity)
                        : StatMacDescriptor(descriptor, out identity);
                if (result == 0)
                {
                    return identity.Mode;
                }
                throw MetadataError(path, Marshal.GetLastWin32Error());
            }

            bool x64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
            try
            {
                if (
                    StatLinux(
                        descriptor,
                        string.Empty,
                        LinuxEmptyPath,
                        LinuxStatType,
                        out LinuxFileIdentity identity
                    ) == 0
                )
                {
                    return (identity.Mask & LinuxStatType) != 0 ? identity.Mode : 0;
                }

                int error = Marshal.GetLastWin32Error();
                if (
                    !x64
                    || (error is not LinuxOperationUnavailable and not LinuxOperationNotPermitted)
                )
                {
                    throw MetadataError(path, error);
                }
            }
            catch (EntryPointNotFoundException) when (x64) { }

            if (
                StatLegacyLinuxDescriptor(
                    LinuxX64StatVersion,
                    descriptor,
                    out LegacyLinuxFileIdentity legacy
                ) != 0
            )
            {
                throw MetadataError(path, Marshal.GetLastWin32Error());
            }

            return (int)legacy.Mode;
        }

        [DllImport("libc", EntryPoint = "open", ExactSpelling = true, SetLastError = true)]
        private static extern int OpenSourceLinux(string path, int flags);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "open",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int OpenSourceMac(string path, int flags);

        private static FileStream AcquireUnix(string lockPath, bool isMac)
        {
            DurableFileOwnershipStream stream;
            bool created;
            try
            {
                stream = new DurableFileOwnershipStream(lockPath, FileMode.CreateNew);
                created = true;
            }
            catch (IOException) when (File.Exists(lockPath))
            {
                RejectLockReparsePoint(lockPath);
                stream = new DurableFileOwnershipStream(lockPath, FileMode.Open);
                created = false;
            }

            try
            {
                int descriptor = stream.SafeFileHandle.DangerousGetHandle().ToInt32();
                int operation = LockExclusive | LockNonBlocking;
                int result;
                if (isMac)
                {
                    result = LockMac(descriptor, operation);
                }
                else
                {
                    result = LockLinux(descriptor, operation);
                }

                if (result != 0)
                {
                    int nativeError = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Could not acquire writer ownership at '{lockPath}' (OS error {nativeError})."
                    );
                }

                bool matches;
                if (isMac)
                {
                    matches = MatchesMacIdentity(descriptor, stream.Name);
                }
                else
                {
                    matches = MatchesLinuxIdentity(descriptor, stream.Name);
                }

                if (!matches)
                {
                    throw new IOException(
                        $"Writer ownership changed at '{lockPath}'. Retry the operation."
                    );
                }
                if (!created)
                {
                    RequireEmptyLock(stream, lockPath);
                }
                stream._ownsName = true;
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static bool MatchesLinuxIdentity(int descriptor, string lockPath)
        {
            LinuxFileIdentity opened;
            try
            {
                int result = StatLinux(
                    descriptor,
                    string.Empty,
                    LinuxEmptyPath,
                    LinuxStatInode,
                    out opened
                );
                if (result != 0)
                {
                    int nativeError = Marshal.GetLastWin32Error();
                    if (
                        RuntimeInformation.ProcessArchitecture == Architecture.X64
                        && nativeError is LinuxOperationNotPermitted or LinuxOperationUnavailable
                    )
                    {
                        return MatchesLegacyLinuxIdentity(descriptor, lockPath);
                    }
                    throw MetadataError(lockPath, nativeError);
                }
            }
            catch (EntryPointNotFoundException)
                when (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                return MatchesLegacyLinuxIdentity(descriptor, lockPath);
            }

            if (
                StatLinux(
                    LinuxCurrentDirectory,
                    lockPath,
                    LinuxSymlinkNoFollow,
                    LinuxStatInode,
                    out LinuxFileIdentity named
                ) != 0
            )
            {
                int nativeError = Marshal.GetLastWin32Error();
                throw MetadataError(lockPath, nativeError);
            }

            return (opened.Mask & LinuxStatInode) != 0
                && (named.Mask & LinuxStatInode) != 0
                && opened.Inode == named.Inode
                && opened.DeviceMajor == named.DeviceMajor
                && opened.DeviceMinor == named.DeviceMinor;
        }

        private static bool MatchesLegacyLinuxIdentity(int descriptor, string lockPath)
        {
            int descriptorResult = StatLegacyLinuxDescriptor(
                LinuxX64StatVersion,
                descriptor,
                out LegacyLinuxFileIdentity opened
            );
            if (descriptorResult != 0)
            {
                int nativeError = Marshal.GetLastWin32Error();
                throw MetadataError(lockPath, nativeError);
            }

            int pathResult = StatLegacyLinuxPath(
                LinuxX64StatVersion,
                lockPath,
                out LegacyLinuxFileIdentity named
            );
            if (pathResult != 0)
            {
                int nativeError = Marshal.GetLastWin32Error();
                throw MetadataError(lockPath, nativeError);
            }
            return opened.Device == named.Device && opened.Inode == named.Inode;
        }

        private static bool MatchesMacIdentity(int descriptor, string lockPath)
        {
            MacFileIdentity opened;
            MacFileIdentity named;
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                if (
                    StatIntelMacDescriptor(descriptor, out opened) != 0
                    || StatIntelMacPath(lockPath, out named) != 0
                )
                {
                    int nativeError = Marshal.GetLastWin32Error();
                    throw MetadataError(lockPath, nativeError);
                }
            }
            else if (
                StatMacDescriptor(descriptor, out opened) != 0
                || StatMacPath(lockPath, out named) != 0
            )
            {
                int nativeError = Marshal.GetLastWin32Error();
                throw MetadataError(lockPath, nativeError);
            }

            return opened.Device == named.Device && opened.Inode == named.Inode;
        }

        private static IOException MetadataError(string path, int nativeError)
        {
            return new IOException(
                $"Could not inspect filesystem metadata for '{path}' (OS error {nativeError})."
            );
        }

        [DllImport("libc", EntryPoint = "flock", ExactSpelling = true, SetLastError = true)]
        private static extern int LockLinux(int descriptor, int operation);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "flock",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int LockMac(int descriptor, int operation);

        [DllImport("libc", EntryPoint = "statx", ExactSpelling = true, SetLastError = true)]
        private static extern int StatLinux(
            int directory,
            string path,
            int flags,
            uint mask,
            out LinuxFileIdentity identity
        );

        [DllImport("libc", EntryPoint = "__fxstat", ExactSpelling = true, SetLastError = true)]
        private static extern int StatLegacyLinuxDescriptor(
            int version,
            int descriptor,
            out LegacyLinuxFileIdentity identity
        );

        [DllImport("libc", EntryPoint = "__lxstat", ExactSpelling = true, SetLastError = true)]
        private static extern int StatLegacyLinuxPath(
            int version,
            string path,
            out LegacyLinuxFileIdentity identity
        );

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "fstat$INODE64",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int StatIntelMacDescriptor(
            int descriptor,
            out MacFileIdentity identity
        );

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "lstat$INODE64",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int StatIntelMacPath(string path, out MacFileIdentity identity);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "fstat",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int StatMacDescriptor(int descriptor, out MacFileIdentity identity);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "lstat",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int StatMacPath(string path, out MacFileIdentity identity);

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownsName)
            {
                // Unlink under ownership. An opener paused before flock will then fail the identity check.
                _ownsName = false;
                DurableFile.DiscardStagedFile(Name);
            }

            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct LinuxFileIdentity
        {
            [FieldOffset(0)]
            internal uint Mask;

            [FieldOffset(28)]
            internal ushort Mode;

            [FieldOffset(32)]
            internal ulong Inode;

            [FieldOffset(136)]
            internal uint DeviceMajor;

            [FieldOffset(140)]
            internal uint DeviceMinor;
        }

        [StructLayout(LayoutKind.Explicit, Size = 144)]
        private struct LegacyLinuxFileIdentity
        {
            [FieldOffset(0)]
            internal ulong Device;

            [FieldOffset(8)]
            internal ulong Inode;

            [FieldOffset(24)]
            internal uint Mode;
        }

        [StructLayout(LayoutKind.Explicit, Size = 144)]
        private struct MacFileIdentity
        {
            [FieldOffset(0)]
            internal int Device;

            [FieldOffset(4)]
            internal ushort Mode;

            [FieldOffset(8)]
            internal ulong Inode;
        }
#endif
    }
}
