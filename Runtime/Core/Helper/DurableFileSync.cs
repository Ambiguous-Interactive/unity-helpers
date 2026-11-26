// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.Win32.SafeHandles;
    using HostRuntimeInformation = System.Runtime.InteropServices.RuntimeInformation;

    internal sealed class DurableFileSync : IDisposable
    {
#if UNITY_EDITOR || UNITY_STANDALONE || !UNITY_5_3_OR_NEWER
        private const int Interrupted = 4;
        private const int MacInvalidArgument = 22;
        private const int MacInappropriateIoctl = 25;
        private const int MacOperationUnsupported = 45;
        private const int MacFullSync = 51;
        private const int MacOperationNotSupportedOnSocket = 102;
        private const uint FixedDrive = 3;
        private const uint GenericWrite = 0x40000000;
        private const uint DeleteAccess = 0x00010000;
        private const uint OpenReparsePoint = 0x00200000;
        private const int FileDispositionInfoEx = 21;
        private const uint DeleteWithPosixSemantics = 3;
        private const uint BackupSemantics = 0x02000000;

        private readonly SafeFileHandle _windowsHandle;
        private readonly LinuxDirectoryHandle _linuxDirectory;
        private readonly int _linuxDescriptor;
        private readonly string _path;

        private DurableFileSync(string path, SafeFileHandle handle)
        {
            _path = path;
            _windowsHandle = handle;
        }

        private DurableFileSync(string path, LinuxDirectoryHandle directory, int descriptor)
        {
            _path = path;
            _linuxDirectory = directory;
            _linuxDescriptor = descriptor;
        }

        internal static DurableFileSync Open(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return OpenWindows(path);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OpenLinux(path);
            }
            throw DirectorySyncUnavailable();
        }

        private static DurableFileSync OpenLinux(string path)
        {
            IntPtr opened = OpenDirectory(path);
            if (opened == IntPtr.Zero)
            {
                throw NativeFailure("open", path, Marshal.GetLastWin32Error());
            }

            LinuxDirectoryHandle directory = new(opened);
            try
            {
                int descriptor = GetDirectoryDescriptor(opened);
                if (descriptor < 0)
                {
                    throw NativeFailure("open", path, Marshal.GetLastWin32Error());
                }

                return new DurableFileSync(path, directory, descriptor);
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        internal static void FlushFileData(FileStream stream)
        {
            bool linux = HostRuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            bool mac = HostRuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            bool windows = HostRuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (linux || mac || windows)
            {
                // Drain the managed buffer, then check the OS barrier result on this exact handle.
                stream.Flush();
                SafeFileHandle handle = stream.SafeFileHandle;
                if (windows)
                {
                    FlushWindowsHandle(handle, stream.Name);
                    return;
                }

                bool acquired = false;
                try
                {
                    handle.DangerousAddRef(ref acquired);
                    int descriptor = handle.DangerousGetHandle().ToInt32();
                    FlushUnixDescriptor(descriptor, stream.Name, mac);
                }
                finally
                {
                    if (acquired)
                    {
                        handle.DangerousRelease();
                    }
                }
                return;
            }
            stream.Flush(flushToDisk: true);
        }

        private static void FlushWindowsHandle(SafeFileHandle handle, string path)
        {
            if (!FlushFileBuffers(handle))
            {
                throw NativeFailure("synchronize", path, Marshal.GetLastWin32Error());
            }
        }

        private static void FlushUnixDescriptor(int descriptor, string path, bool mac)
        {
            if (mac)
            {
                while (FullSyncMac(descriptor, MacFullSync) != 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (
                        error
                        is MacInvalidArgument
                            or MacInappropriateIoctl
                            or MacOperationUnsupported
                            or MacOperationNotSupportedOnSocket
                    )
                    {
                        // Some volumes accept fsync but do not support the Apple hardware flush.
                        while (SyncMac(descriptor) != 0)
                        {
                            error = Marshal.GetLastWin32Error();
                            if (error != Interrupted)
                            {
                                throw NativeFailure("synchronize", path, error);
                            }
                        }
                        return;
                    }

                    if (error != Interrupted)
                    {
                        throw NativeFailure("synchronize", path, error);
                    }
                }

                return;
            }

            while (SyncLinux(descriptor) != 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != Interrupted)
                {
                    throw NativeFailure("synchronize", path, error);
                }
            }
        }

        private static DurableFileSync OpenWindows(string path)
        {
            if (GetDriveType(Path.GetPathRoot(path)) != FixedDrive)
            {
                throw new PlatformNotSupportedException(
                    "Parent directory synchronization requires a local fixed NTFS drive on Windows."
                );
            }

            SafeFileHandle handle = OpenWindowsEntry(
                DurableFile.GetWindowsNativePath(path),
                GenericWrite,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                BackupSemantics,
                IntPtr.Zero
            );
            try
            {
                if (handle.IsInvalid)
                {
                    throw NativeFailure("open", path, Marshal.GetLastWin32Error());
                }

                StringBuilder filesystem = new(32);
                if (
                    !GetVolumeInformation(
                        handle,
                        IntPtr.Zero,
                        0,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        filesystem,
                        (uint)filesystem.Capacity
                    )
                )
                {
                    throw NativeFailure(
                        "identify the filesystem for",
                        path,
                        Marshal.GetLastWin32Error()
                    );
                }

                // Microsoft's file-system protocol, section 2.1.5.7, specifies a directory persistence effect on NTFS.
                if (
                    !string.Equals(
                        filesystem.ToString(),
                        "NTFS",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new PlatformNotSupportedException(
                        $"Parent directory synchronization requires NTFS: '{path}'."
                    );
                }

                return new DurableFileSync(path, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static IOException NativeFailure(string action, string path, int error)
        {
            return new IOException($"Could not {action} '{path}' (OS error {error}).");
        }

        [DllImport("libc", EntryPoint = "opendir", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr OpenDirectory(string path);

        [DllImport("libc", EntryPoint = "dirfd", ExactSpelling = true, SetLastError = true)]
        private static extern int GetDirectoryDescriptor(IntPtr directory);

        [DllImport("libc", EntryPoint = "fsync", ExactSpelling = true, SetLastError = true)]
        private static extern int SyncLinux(int descriptor);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "fsync",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int SyncMac(int descriptor);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "fcntl",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int FullSyncMac(int descriptor, int command);

        [DllImport("libc", EntryPoint = "closedir", ExactSpelling = true)]
        private static extern int CloseDirectory(IntPtr directory);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "GetDriveTypeW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true
        )]
        private static extern uint GetDriveType(string root);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern SafeFileHandle OpenWindowsEntry(
            string path,
            uint access,
            FileShare sharing,
            IntPtr security,
            FileMode disposition,
            uint flags,
            IntPtr template
        );

        [DllImport(
            "kernel32.dll",
            EntryPoint = "GetVolumeInformationByHandleW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            SetLastError = true
        )]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformation(
            SafeFileHandle handle,
            IntPtr volume,
            uint volumeLength,
            IntPtr serial,
            IntPtr maxComponentLength,
            IntPtr flags,
            StringBuilder filesystem,
            uint filesystemLength
        );

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle handle,
            int informationClass,
            ref uint value,
            uint size
        );

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle handle);

        public void Dispose()
        {
            _windowsHandle?.Dispose();
            _linuxDirectory?.Dispose();
        }

        internal void Flush()
        {
            if (_windowsHandle != null)
            {
                FlushWindowsHandle(_windowsHandle, _path);
            }
            else
            {
                bool acquired = false;
                try
                {
                    _linuxDirectory.DangerousAddRef(ref acquired);
                    FlushUnixDescriptor(_linuxDescriptor, _path, mac: false);
                }
                finally
                {
                    if (acquired)
                    {
                        _linuxDirectory.DangerousRelease();
                    }
                }
            }
        }

        internal void Delete(string path)
        {
            if (_windowsHandle == null)
            {
                File.Delete(path);
                return;
            }

            using SafeFileHandle file = OpenWindowsEntry(
                DurableFile.GetWindowsNativePath(path),
                DeleteAccess,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                OpenReparsePoint,
                IntPtr.Zero
            );
            if (file.IsInvalid)
            {
                throw new IOException(
                    $"Could not open '{path}' for deletion (OS error {Marshal.GetLastWin32Error()})."
                );
            }

            // Remove the name when this handle closes, even if delete-sharing readers remain.
            // The subsequent parent flush must see the unlink rather than a deferred deletion.
            uint flags = DeleteWithPosixSemantics;
            if (!SetFileInformationByHandle(file, FileDispositionInfoEx, ref flags, sizeof(uint)))
            {
                throw new IOException(
                    $"Could not unlink '{path}' (OS error {Marshal.GetLastWin32Error()})."
                );
            }
        }

        private sealed class LinuxDirectoryHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal LinuxDirectoryHandle(IntPtr directory)
                : base(ownsHandle: true)
            {
                SetHandle(directory);
            }

            protected override bool ReleaseHandle()
            {
                return CloseDirectory(handle) == 0;
            }
        }
#else
        internal static DurableFileSync Open(string path)
        {
            throw DirectorySyncUnavailable();
        }

        internal static void FlushFileData(FileStream stream)
        {
            stream.Flush(flushToDisk: true);
        }

        public void Dispose() { }

        internal void Flush()
        {
            throw DirectorySyncUnavailable();
        }

        internal void Delete(string path)
        {
            throw DirectorySyncUnavailable();
        }
#endif

        private static PlatformNotSupportedException DirectorySyncUnavailable()
        {
            return new PlatformNotSupportedException(
                "Parent directory synchronization is supported on Linux and local Windows NTFS."
            );
        }
    }
}
